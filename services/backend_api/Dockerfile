# syntax=docker/dockerfile:1.7
# Multi-stage build for the dental commerce backend.
# Runtime base is chiseled-extra because it includes tzdata (needed by
# NodaTime for Asia/Riyadh + Africa/Cairo in the inventory expiry sweeper)
# and ICU (needed by Arabic normalization in the search module).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY backend_api.csproj ./
RUN dotnet restore backend_api.csproj
COPY . .
RUN dotnet publish backend_api.csproj -c Release -o /out --no-restore /p:UseAppHost=false \
 && mkdir -p /out/tmp/storage

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled-extra AS runtime
COPY --from=build --chown=10001:10001 /out /app
WORKDIR /app
USER 10001:10001
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "backend_api.dll"]
