ARG LVM_VERSION=0.2.0-dev
ARG LVM_CHANNEL=development
ARG LVM_BUILD=local
ARG LVM_COMMIT=unknown

FROM node:24-bookworm-slim@sha256:55fb5f5a527ea2c5c4a95f963e48ef90aa38c7b346054fdc8ee9957a2b55d205 AS frontend-build
ARG LVM_VERSION
WORKDIR /src/LyrionVoiceMcp.Web
COPY LyrionVoiceMcp.Web/package.json LyrionVoiceMcp.Web/package-lock.json LyrionVoiceMcp.Web/.npmrc ./
RUN npm ci --ignore-scripts
COPY LyrionVoiceMcp.Web/ ./
ENV VITE_LVM_VERSION=$LVM_VERSION
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS backend-build
WORKDIR /src
COPY Directory.Build.props global.json LyrionVoiceMcp.slnx ./
COPY LyrionVoiceMcp.Abstractions/ LyrionVoiceMcp.Abstractions/
COPY LyrionVoiceMcp.Api/ LyrionVoiceMcp.Api/
COPY LyrionVoiceMcp.Api.Tests/ LyrionVoiceMcp.Api.Tests/
COPY LyrionVoiceMcp.Contracts/ LyrionVoiceMcp.Contracts/
COPY LyrionVoiceMcp.Dev/ LyrionVoiceMcp.Dev/
COPY LyrionVoiceMcp.Dev.Tests/ LyrionVoiceMcp.Dev.Tests/
COPY LyrionVoiceMcp.Ef/ LyrionVoiceMcp.Ef/
COPY LyrionVoiceMcp.Ef.Abstractions/ LyrionVoiceMcp.Ef.Abstractions/
COPY LyrionVoiceMcp.Evaluation/ LyrionVoiceMcp.Evaluation/
COPY LyrionVoiceMcp.Lms/ LyrionVoiceMcp.Lms/
COPY LyrionVoiceMcp.Persistence.Tests/ LyrionVoiceMcp.Persistence.Tests/
COPY LyrionVoiceMcp.Search/ LyrionVoiceMcp.Search/
COPY LyrionVoiceMcp.Search.Tests/ LyrionVoiceMcp.Search.Tests/
COPY LyrionVoiceMcp.Services/ LyrionVoiceMcp.Services/
COPY LyrionVoiceMcp.Services.Tests/ LyrionVoiceMcp.Services.Tests/
RUN dotnet restore LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj --locked-mode -maxcpucount:1 -nodeReuse:false
RUN dotnet publish LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj --configuration Release --no-restore --output /app/publish -maxcpucount:1 -nodeReuse:false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
ARG LVM_VERSION
ARG LVM_CHANNEL
ARG LVM_BUILD
ARG LVM_COMMIT
WORKDIR /app
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /src/LyrionVoiceMcp.Web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://0.0.0.0:5600
ENV LyrionVoiceMcpBuild__Version=$LVM_VERSION
ENV LyrionVoiceMcpBuild__Channel=$LVM_CHANNEL
ENV LyrionVoiceMcpBuild__Build=$LVM_BUILD
ENV LyrionVoiceMcpBuild__Commit=$LVM_COMMIT
ENV LyrionVoiceMcpPersistence__DatabasePath=/data/lyrion-voice-mcp.db
ENV LyrionVoiceMcpSearch__IndexDirectoryPath=/data/search-index
EXPOSE 5600
VOLUME ["/data"]
USER $APP_UID
ENTRYPOINT ["dotnet", "LyrionVoiceMcp.Api.dll"]
