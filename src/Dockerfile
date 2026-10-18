FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
ARG PROJECT
WORKDIR /repo
COPY nuget.config Directory.Build.props .editorconfig ./
COPY fonts/ fonts/
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV APP_ASSEMBLY=${PROJECT}.dll
USER app
ENTRYPOINT ["sh", "-c", "exec dotnet /app/${APP_ASSEMBLY}"]
