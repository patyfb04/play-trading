FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src

# Copy nuget.config and packages folder
COPY nuget.config ./
COPY packages ./packages

COPY ["Play.Trading/src/Play.Trading.Service/Play.Trading.Service.csproj", "Play.Trading/src/Play.Trading.Service/"]
RUN dotnet restore "Play.Trading/src/Play.Trading.Service/Play.Trading.Service.csproj"
COPY . .
WORKDIR "/src/Play.Trading/src/Play.Trading.Service"
RUN dotnet build "Play.Trading.Service.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "Play.Trading.Service.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Play.Trading.Service.dll"]
