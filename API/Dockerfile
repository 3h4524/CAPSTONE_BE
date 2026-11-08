FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY Domain/Domain.csproj Domain/
COPY Common/Common.csproj Common/
COPY Application/Application.csproj Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY API/API.csproj API/
RUN dotnet restore API/API.csproj
COPY Domain/ Domain/
COPY Common/ Common/
COPY Application/ Application/
COPY Infrastructure/ Infrastructure/
COPY API/ API/
RUN dotnet publish API/API.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fontconfig fonts-dejavu-core libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /app/models /data/keyring && chown -R app:app /app/models /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=120s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "APCS.Api.dll"]
