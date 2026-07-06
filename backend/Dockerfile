FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY backend/src/LootBase.Api/LootBase.Api.csproj backend/src/LootBase.Api/
COPY backend/src/LootBase.Application/LootBase.Application.csproj backend/src/LootBase.Application/
COPY backend/src/LootBase.Domain/LootBase.Domain.csproj backend/src/LootBase.Domain/
COPY backend/src/LootBase.Infrastructure/LootBase.Infrastructure.csproj backend/src/LootBase.Infrastructure/
RUN dotnet restore backend/src/LootBase.Api/LootBase.Api.csproj

COPY backend/src backend/src
RUN dotnet publish backend/src/LootBase.Api/LootBase.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5188
COPY --from=build /app/publish .

EXPOSE 5188
ENTRYPOINT ["dotnet", "LootBase.Api.dll"]
