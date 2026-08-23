# Lyrion Voice MCP

This is very early pre release code. It aims to provide a MCP server for Lyrion / LMS that maintains its own index so that it can do phoenetic and fuzzy searches to account for voice agents struggling with artist and tack names.

To do this is has to scan and index the libray first - this is currently a manual process that has to be initiated from its website.


> [!WARNING]
> The application has no authentication. It is intended for a trusted local network and must not be exposed directly to the public internet.


## Requirements

- .NET SDK 10.0.302 or a compatible 10.0 patch
- Node.js 24
- Docker 29 or later for container validation

## Development

Install frontend dependencies once:

```sh
cd LyrionVoiceMcp.Web
npm ci
```

From the repository root, run `./dev.sh` and press `A` to start the API and Vite. The API listens on `5600` and Vite on `5175`, deliberately differing from neighbouring BoardOil and KST projects. `./dev-startall.sh` provides an unattended equivalent; PowerShell variants are included.

The launchers load ignored machine-local LMS settings from `.data/dev/appsettings.local.json`:

```json
{
  "LyrionVoiceMcpLms": {
    "ServerId": "development",
    "BaseUrl": "http://lms-hostname-or-address:9000",
    "RequestTimeoutSeconds": 5
  }
}
```
## Validation

```sh
./scripts/test-fast.sh
./scripts/test-full.sh
```

The fast script selects affected lanes when Git history is available. The full script restores, builds, tests backend and frontend projects, and runs repository checks.

## Container

```sh
LVM_LMS_SERVER_ID=development \
LVM_LMS_BASE_URL=http://lms-hostname-or-address:9000 \
docker compose up --build
```

The image serves the complete application on port `5600`. CI validates `linux/amd64` and `linux/arm64` builds. Compose persists the application database and search-index artifacts under `/data` in the `application-data` named volume.

## Licence

Lyrion Voice MCP is released under the [MIT licence](LICENSE).
