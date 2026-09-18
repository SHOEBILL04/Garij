# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for caching dependency layers
COPY ["src/Garij.Domain/Garij.Domain.csproj", "src/Garij.Domain/"]
COPY ["src/Garij.Application/Garij.Application.csproj", "src/Garij.Application/"]
COPY ["src/Garij.Infrastructure/Garij.Infrastructure.csproj", "src/Garij.Infrastructure/"]
COPY ["src/Garij.Web/Garij.Web.csproj", "src/Garij.Web/"]

# Restore NuGet dependencies
RUN dotnet restore "src/Garij.Web/Garij.Web.csproj"

# Copy source code and build
COPY src/ src/
WORKDIR "/src/src/Garij.Web"
RUN dotnet publish "Garij.Web.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure SQLite can create files in working directory
RUN mkdir -p /app/data && chown -R app:app /app

USER app
COPY --from=build --chown=app:app /app/publish .

# Default configuration for cloud hosting
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Garij.Web.dll"]
