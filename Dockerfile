FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SGRNetGuard.Backend/Api/SGRNetGuard.Api.csproj SGRNetGuard.Backend/Api/
RUN dotnet restore SGRNetGuard.Backend/Api/SGRNetGuard.Api.csproj

COPY SGRNetGuard.Backend/Api/ SGRNetGuard.Backend/Api/
RUN dotnet publish SGRNetGuard.Backend/Api/SGRNetGuard.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}
ENTRYPOINT ["dotnet", "SGRNetGuard.Api.dll"]