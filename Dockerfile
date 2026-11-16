# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Rethread.Api/Rethread.Api.csproj src/Rethread.Api/
RUN dotnet restore src/Rethread.Api/Rethread.Api.csproj
COPY src/ src/
RUN dotnet publish src/Rethread.Api/Rethread.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# The image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Rethread.Api.dll"]
