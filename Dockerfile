# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000

# Build image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
ARG GITHUB_TOKEN
ENV NUGET_AUTH_TOKEN=$GITHUB_TOKEN

WORKDIR /src

COPY nuget.config ./

# Copy entire src folder preserving structure
COPY src/ ./src/

# Add GitHub Packages feed
RUN dotnet nuget remove source github || true
RUN dotnet nuget add source \
    --username patyfb04 \
    --password $GITHUB_TOKEN \
    --store-password-in-clear-text \
    --name github \
    https://nuget.pkg.github.com/patyfb04/index.json


# Restore
RUN dotnet restore src/Play.Trading.Service/Play.Trading.Service.csproj

# Build
WORKDIR /src/src/Play.Trading.Service
RUN dotnet build "Play.Trading.Service.csproj" -c $configuration -o /app/build

# Publish
FROM build AS publish
ARG configuration=Release
RUN dotnet publish "Play.Trading.Service.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Play.Trading.Service.dll"]