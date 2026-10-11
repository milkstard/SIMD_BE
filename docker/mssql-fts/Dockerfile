# SQL Server 2022 + Full-Text Search. The stock image has no FTS, and migration AddIncidents creates a full-text index.
# Used by docker-compose.yml (service "mssql") and by the integration tests (Testcontainers builds it on first run).
FROM mcr.microsoft.com/mssql/server:2022-latest

USER root

# The base image normally already has the Microsoft package repository configured; add it only when the package is not found.
RUN apt-get update \
    && ( apt-get install -y --no-install-recommends mssql-server-fts \
         || ( apt-get install -y --no-install-recommends curl gnupg ca-certificates \
              && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
              && curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list -o /etc/apt/sources.list.d/mssql-server-2022.list \
              && apt-get update \
              && apt-get install -y --no-install-recommends mssql-server-fts ) ) \
    && rm -rf /var/lib/apt/lists/*

USER mssql
