# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/CampaignEngine.Core/CampaignEngine.Core.csproj src/CampaignEngine.Core/
COPY src/CampaignEngine.Infrastructure/CampaignEngine.Infrastructure.csproj src/CampaignEngine.Infrastructure/
COPY src/CampaignEngine.Api/CampaignEngine.Api.csproj src/CampaignEngine.Api/
RUN dotnet restore src/CampaignEngine.Api/CampaignEngine.Api.csproj
COPY src/ src/
RUN dotnet publish src/CampaignEngine.Api/CampaignEngine.Api.csproj -c Release -o /app --no-restore

# The Debian-based image ships tzdata and ICU, which the engine needs for IANA time zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Database__ConnectionString="Data Source=/data/campaigns.db"
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "CampaignEngine.Api.dll"]
