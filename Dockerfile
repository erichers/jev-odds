FROM node:24-bookworm AS ui
WORKDIR /src
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/JevOdds.Api/ src/JevOdds.Api/
RUN dotnet publish src/JevOdds.Api/JevOdds.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=ui /src/dist/client/browser ./wwwroot
USER root
RUN mkdir -p /data && chown app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__Odds=Data Source=/data/jev-odds.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "JevOdds.Api.dll"]
