FROM node:22-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402 AS live
WORKDIR /repo/src/StormBench.Live
COPY src/StormBench.Live/package.json src/StormBench.Live/package-lock.json src/StormBench.Live/.npmrc ./
RUN npm ci
COPY src/StormBench.Live/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
ARG PROJECT
WORKDIR /repo
COPY nuget.config Directory.Build.props .editorconfig ./
COPY fonts/ fonts/
COPY src/ src/
COPY --from=live /repo/src/StormBench.Scada/wwwroot/live/dist/ src/StormBench.Scada/wwwroot/live/dist/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app -p:BuildLive=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV APP_ASSEMBLY=${PROJECT}.dll
USER app
ENTRYPOINT ["sh", "-c", "exec dotnet /app/${APP_ASSEMBLY}"]
