FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props SoloLeveling.slnx ./
COPY src/SoloLeveling.Domain/SoloLeveling.Domain.csproj src/SoloLeveling.Domain/
COPY src/SoloLeveling.Infrastructure/SoloLeveling.Infrastructure.csproj src/SoloLeveling.Infrastructure/
COPY src/SoloLeveling.Api/SoloLeveling.Api.csproj src/SoloLeveling.Api/
RUN dotnet restore src/SoloLeveling.Api/SoloLeveling.Api.csproj
COPY src/ src/
RUN dotnet publish src/SoloLeveling.Api/SoloLeveling.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql 啟動時會探測 Kerberos 函式庫，沒裝會在 log 印一行 Error（不影響功能）；curl 給 compose healthcheck 用
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SoloLeveling.Api.dll"]
