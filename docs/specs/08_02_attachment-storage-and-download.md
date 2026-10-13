# Spec 08_02: Attachment storage, upload & download

> Status: **Draft** · Source: BE-08 in docs/BACKEND-SPECS.md · Part 02 of 03

## 1. Purpose & scope

The attachment pipeline, independent of comments: a user uploads a file to blob storage (it starts `Pending`), a scan hook
marks it `Clean` or `Infected`, and a `Clean` file is downloaded through a short-lived SAS link. Linking an attachment to an
incident or a comment is 08_03.

In scope:
- `Attachment` entity, `Attachments` table + migration (`AddAttachments`), `IAppDbContext.Attachments`.
- `IFileStore` abstraction (Application) and its Azure Blob implementation (Infrastructure); Azurite locally.
- `POST /api/v1/attachments` — multipart upload, ≤ 25 MB, allowed types only, `scanStatus: Pending`.
- Virus-scan hook (stub in v1): `IAttachmentScanner` + a stub implementation, run by a Worker job.
- `GET /api/v1/attachments/{id}/download` — `302` to a 5-minute SAS link, `409` unless `Clean`.

Pulled in from `deferred-items.md`: "`Comment`/`Attachment` entities, tables and events" — the **Attachment** half
(`AttachmentAdded` is raised by 08_03 when the attachment is linked).

## 2. Dependencies

- BE-02 (`ICurrentUser`, policies), 05_02 (size-cap pattern, `IdempotencyFilter`), docker-compose (Azurite, already present).
- 08_01 is not required. 08_03 depends on this part.

## 3. Design

### Domain
- `Incidents/Attachment` entity: `IncidentId?` (null until linked — OQ 1), `CommentId?` (set when linked to a comment), `UploadedById`, `FileName` (≤ 300), `ContentType` (≤ 100), `SizeBytes` (> 0, ≤ `Attachment.MaxSizeBytes` = 25 MiB), `BlobPath` (≤ 500, never exposed), `ScanStatus` (`Pending | Clean | Infected`), `CreatedAt`.
- `ScanStatus` enum in Domain with identical names to the contract.
- Behaviour: `Attachment.Create(...)` (validates size/name), `MarkScanned(ScanStatus result)` — only `Pending → Clean|Infected`, anything else is an `InvalidOperationException` (idempotent re-scan with the same result is a no-op).
- `AttachmentRules` (static): allowed extensions and content types — `image/png`, `image/jpeg`, `image/gif`, `image/webp`, `text/plain` (`.txt`, `.log`), `application/json` (`.json`) — and file-name sanitising (strip paths, control chars; max 300). SVG is **not** allowed (script-capable, OQ 3).

### Application
`Abstractions/IFileStore` (CLAUDE.md names it): `UploadAsync(string path, Stream content, string contentType, ct)`, `DeleteAsync(path, ct)`, `CreateDownloadUriAsync(path, fileName, TimeSpan lifetime, ct) : Uri`, `OpenReadAsync(path, ct)` (scanner only).
`Abstractions/IAttachmentScanner`: `ScanAsync(Stream content, ct) : ScanStatus`.

`Attachments/UploadAttachment/`
- `UploadAttachmentCommand(string FileName, string ContentType, long Length, Stream Content) : IRequest<AttachmentDto>`.
- `UploadAttachmentValidator`: file name present; extension and content type in the allow-list **and consistent with each other**; `0 < length ≤ 25 MiB` (otherwise `413` for size via `PayloadTooLargeException`, `422` on `file` for type/empty — OQ 4).
- Handler: derive `BlobPath` = `attachments/{yyyy}/{MM}/{attachmentId}` (server-generated, never from the file name); content sniffing of the first bytes for images (magic numbers must match the declared type; `422` on `file` otherwise); stream to `IFileStore.UploadAsync` (never buffer the 25 MB in memory); insert the `Attachment` row (`Pending`); if the insert fails, delete the blob best-effort. Log `"Attachment {AttachmentId} uploaded ({SizeBytes} bytes)"`.
- Returns `AttachmentDto` (`uploadedBy` = caller).

`Attachments/GetAttachmentDownload/`
- `GetAttachmentDownloadQuery(Guid AttachmentId) : IRequest<Uri>`. Handler: load (`AsNoTracking`); missing → 404; access check (OQ 2); `ScanStatus != Clean` → `AttachmentNotReadyException` (`409`, title "Attachment not available", detail names the status); else `IFileStore.CreateDownloadUriAsync(..., TimeSpan.FromMinutes(5))` with `Content-Disposition: attachment; filename=...`.

### Infrastructure
- `Storage/AzureBlobFileStore : IFileStore` (`Azure.Storage.Blobs`); container `attachments`, private access. SAS is **user-delegation** in Azure (managed identity) and account-key SAS against Azurite; read-only, 5-minute expiry, `Content-Disposition: attachment`, `Content-Type` forced to `application/octet-stream` for non-images. Spans on `InfrastructureTelemetry.Source` for upload and SAS creation. Polly retry/timeout on blob calls.
- Options `Storage:Blob` (`ConnectionString` or `AccountUri`, `Container`); local dev uses the Azurite connection string from user-secrets, never appsettings. The container is created by the migration/bootstrap step, not at request time (OQ 5).
- `Persistence/Configurations/AttachmentConfiguration`: table `Attachments`; indexes `(IncidentId)` (DATABASE.md) and a filtered `(UploadedById, CreatedAt) WHERE IncidentId IS NULL` for orphan cleanup; FKs `Restrict`. Migration `AddAttachments` — DATABASE.md currently says `IncidentId` is **not null**; see OQ 1.
- `Security/StubAttachmentScanner : IAttachmentScanner` — marks `Clean` unless the content starts with the EICAR test string (then `Infected`); registered behind the interface so a real scanner (ClamAV / Defender for Storage) replaces it later.

### Api
`Controllers/AttachmentsController` (`api/v1/attachments`):
- `[HttpPost] [Authorize(Policy = Policies.CanReportIncident)] [DisableFormValueModelBinding] [RequestSizeLimit(26_214_400 + overhead)]` — reads the multipart stream with `MultipartReader`; exactly one part named `file`; more parts or a missing file → `422`/`400`. Not `[Idempotent]` (an upload is a blob, no body-hash).
- `[HttpGet("{id:guid}/download")] [Authorize(Policy = Policies.CanViewIncident)]` → `Redirect(uri.ToString())` (`302`); response `Cache-Control: no-store`.
- Kestrel/`FormOptions` limits for this endpoint only; other endpoints keep their caps.
- Responses never contain `BlobPath`.

### Worker
`AttachmentScanJob : BackgroundService` — every 10 s claims up to 20 `Pending` attachments (`UPDLOCK, READPAST`, oldest first), streams each through `IAttachmentScanner`, `MarkScanned`, saves. Failures leave the row `Pending` and log; a row `Pending` for more than 1 hour is logged at warning (OQ 6). Single instance guarded by the Redis lock used by `SlaMonitor`.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| POST | `/api/v1/attachments` | `multipart/form-data`, field `file` | `201` `Attachment` (`scanStatus: Pending`), `Location` | 400 not multipart / more than one file part; 401; 413 over 25 MB; 422 disallowed or mismatched type, empty file |
| GET | `/api/v1/attachments/{id}/download` | — | `302` to a 5-minute SAS URL | 401; 404 unknown or not visible; 409 not `Clean` |

`Attachment`/`ScanStatus` exist in API-CONTRACT §3/§4. Update `API-CONTRACT.md`: allowed types list, `422` vs `413` rules, `409` detail, that an unlinked upload is visible only to its uploader, and that the download link expires after 5 minutes. Regenerate `swagger.json` (multipart request with a binary `file`).

## 5. Acceptance criteria

- [ ] Upload ≤ 25 MB of an allowed type → `201`, `scanStatus: Pending`, the blob exists in the store and the row stores a server-generated `BlobPath` that never appears in any response.
- [ ] Oversized upload → `413` (checked while streaming, without reading the whole body); disallowed or mismatched type, or an empty file → `422`.
- [ ] Download of a `Pending` or `Infected` attachment → `409`; `Clean` → `302` whose SAS link is read-only and expires 5 minutes after issue (assert the `se` parameter).
- [ ] The API never streams a blob (download is a redirect only; no `FileStreamResult` in the controller).
- [ ] The scan job moves `Pending` rows to `Clean`, or to `Infected` for the EICAR test file; an `Infected` file stays undownloadable.
- [ ] A caller who may not see the attachment gets `404` on download (OQ 2); an anonymous caller gets `401`.
- [ ] A failed row insert leaves no orphan blob (best-effort delete verified), a failed upload leaves no row.
- [ ] `IFileStore`/`IAttachmentScanner` live in Application, implementations in Infrastructure (layering tests green); no secrets in appsettings; build + `dotnet format` clean; `API-CONTRACT.md` + `swagger.json` updated.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Incidents/`
- `AttachmentTests`: `Create_ValidValues_IsPending`, `Create_OverMaxSize_Throws`, `MarkScanned_FromPending_Sets`, `MarkScanned_FromClean_ToInfected_Throws`, `MarkScanned_SameResultTwice_IsNoOp`.
- `AttachmentRulesTests` (`[Theory]`): allowed/disallowed extension × content type pairs, SVG rejected, path traversal in the file name sanitised.

`tests/IncidentHub.Application.UnitTests/Attachments/`
- `UploadAttachmentValidatorTests`: `Validate_DisallowedType_HasError`, `Validate_TypeExtensionMismatch_HasError`, `Validate_EmptyFile_HasError`.
- `UploadAttachmentHandlerTests` (NSubstitute `IFileStore`): `Handle_ValidFile_UploadsAndStoresPending`, `Handle_MagicNumberMismatch_Throws`, `Handle_InsertFails_DeletesBlob`.
- `GetAttachmentDownloadHandlerTests`: `Handle_Pending_ThrowsNotReady`, `Handle_Infected_ThrowsNotReady`, `Handle_Clean_RequestsFiveMinuteLink`.

`tests/IncidentHub.Api.IntegrationTests/Attachments/` — `[Trait("Category","Integration")]`, Testcontainers SQL + **Azurite** container added to the fixture:
- `UploadAttachmentTests`: `Upload_PngUnder25Mb_Returns201Pending`, `Upload_26Mb_Returns413`, `Upload_Exe_Returns422`, `Upload_NoFilePart_Returns400`, `Upload_NoToken_Returns401`, `Upload_Response_HasNoBlobPath`.
- `DownloadAttachmentTests`: `Download_Pending_Returns409`, `Download_Infected_Returns409`, `Download_Clean_Returns302WithFiveMinuteSas`, `Download_SasLink_ServesTheBlobOnce_ThenExpires` (uses a short-lived SAS from the store directly), `Download_OtherTeamsLinkedAttachment_Returns404`, `Download_UnknownId_Returns404`.
- `AttachmentScanJobTests`: `Scan_CleanFile_MarksClean`, `Scan_EicarFile_MarksInfected`, `Scan_StoreFailure_LeavesPending`.
- `StorageContractTests` for `AzureBlobFileStore` against Azurite: upload/read/delete round-trip, SAS content-disposition.

## 7. Out of scope

- Linking attachments to an incident, a report or a comment; `attachments` in `IncidentDetail`/`Comment`; `AttachmentAdded` event; max-10 rule → 08_03.
- Real antivirus integration (ClamAV, Defender for Storage) — v1 ships the stub behind `IAttachmentScanner`; not scheduled.
- Cleanup of unlinked uploads after a TTL (orphan sweeper) → deferred to 08_03 / ops (listed in deferred items).
- Rate limiting of uploads → the existing cross-cutting "Rate limiting" deferred item.
- Image thumbnails/previews, resumable or chunked upload, per-user storage quotas.

## 8. Open questions

1. **Doc conflict — `Attachments.IncidentId` not null.** DATABASE.md requires it, but the contract uploads (`POST /attachments`) before the incident or comment exists (the report flow in API-CONTRACT §8). Proposal: make `IncidentId` nullable until linked, update DATABASE.md in the same change, and add `CommentId` (nullable). Confirm, or choose a different linking model.
2. **Who may download an unlinked or linked attachment?** Proposal: unlinked → the uploader only; linked → anyone who can see the incident (team member), and an attachment linked to an **internal** comment is hidden from Reporters (`404`). Confirm.
3. **Allowed types.** BACKEND-SPECS says "allowed types only"; API-CONTRACT says "images, `.log`, `.txt`, `.json`". This spec reads "images" as png/jpeg/gif/webp (no SVG, no PDF). Confirm the list.
4. **Status for a bad type.** BACKEND-SPECS accepts `413`/`422`; this spec uses `413` for size and `422` for type. Confirm.
5. **Blob container creation** at startup vs. by the pipeline (CLAUDE.md forbids `Database.Migrate()` at startup but is silent on storage).
6. **Scan SLA.** If the stub scanner is down, files stay `Pending` and cannot be downloaded; is a timeout → `Infected`/alert wanted? Proposed: alert only.
7. **Filename in `Content-Disposition`** is user-controlled; the store sanitises it (see rules), but the Azure SAS header cannot carry non-ASCII — proposed RFC 5987 encoding.
