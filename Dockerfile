# Build: the API and the Blazor WebAssembly client it serves.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Splitto.slnx .editorconfig ./
COPY src/Splitto.Core/Splitto.Core.csproj src/Splitto.Core/
COPY src/Splitto.Web/Splitto.Web.csproj src/Splitto.Web/
COPY src/Splitto.Api/Splitto.Api.csproj src/Splitto.Api/
RUN dotnet restore src/Splitto.Api/Splitto.Api.csproj
COPY src src
RUN dotnet publish src/Splitto.Api/Splitto.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Splitto.Api.dll"]
