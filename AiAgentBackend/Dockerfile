# Base runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000 

# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["AiAgentBackend.csproj", "."]
RUN dotnet restore "AiAgentBackend.csproj"
COPY . .
RUN dotnet build "AiAgentBackend.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "AiAgentBackend.csproj" -c Release -o /app/publish

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create SSL directory and set permissions
RUN mkdir -p /app/ssl-certs && \
    chmod 755 /app/ssl-certs

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "AiAgentBackend.dll"]