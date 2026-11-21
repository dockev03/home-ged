# Image Home-GED : le BFF ASP.NET Core sert le front Vue buildé (une seule image).

# --- 1. Front --------------------------------------------------------------
FROM docker.io/library/node:22-alpine AS front
WORKDIR /src
COPY src/front/package.json src/front/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/front/ ./
RUN npm run build

# --- 2. Back ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS back
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/back/HomeGed.Api/HomeGed.Api.csproj src/back/HomeGed.Api/
RUN dotnet restore src/back/HomeGed.Api/HomeGed.Api.csproj
COPY src/back/ src/back/
# Pas « VERSION » : un ARG devient une variable d'environnement, que MSBuild lit
# comme la propriété Version (sans tenir compte de la casse) ; une valeur comme
# « pr-3 » fait alors échouer NuGet. Déclaré ici pour ne pas invalider le restore.
ARG APP_VERSION=0.0.0-dev
RUN dotnet publish src/back/HomeGed.Api/HomeGed.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:InformationalVersion=${APP_VERSION} -p:UseAppHost=false

# --- 3. Exécution ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=back /app ./
COPY --from=front /src/dist ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080

# Image « chiseled » : ni shell ni gestionnaire de paquets, utilisateur non root.
USER $APP_UID

# L'image n'a ni curl ni wget : l'application s'interroge elle-même.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "HomeGed.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "HomeGed.Api.dll"]
