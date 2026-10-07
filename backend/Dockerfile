FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Demo.Api/Demo.Api.csproj Demo.Api/
RUN dotnet restore Demo.Api/Demo.Api.csproj
COPY Demo.Api/ Demo.Api/
RUN dotnet publish Demo.Api/Demo.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
USER root
RUN sed -i 's|http://|https://|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update -o Acquire::https::Timeout=30 \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Demo.Api.dll"]
