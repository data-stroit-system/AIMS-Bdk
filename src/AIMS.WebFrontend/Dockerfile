# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/AIMS.WebFrontend/AIMS.WebFrontend.csproj", "src/AIMS.WebFrontend/"]
COPY ["src/AIMS.Infrastructure/AIMS.Infrastructure.csproj", "src/AIMS.Infrastructure/"]
COPY ["src/AIMS.Core/AIMS.Core.csproj", "src/AIMS.Core/"]
COPY ["src/AIMS.SharedKernel/AIMS.SharedKernel.csproj", "src/AIMS.SharedKernel/"]
COPY ["src/AIMS.Migrations.PostgreSQL/AIMS.Migrations.PostgreSQL.csproj", "src/AIMS.Migrations.PostgreSQL/"]
COPY ["src/AIMS.Migrations.SqlServer/AIMS.Migrations.SqlServer.csproj", "src/AIMS.Migrations.SqlServer/"]
RUN dotnet restore "./src/AIMS.WebFrontend/AIMS.WebFrontend.csproj"
COPY . .
WORKDIR "/src/src/AIMS.WebFrontend"
RUN dotnet build "./AIMS.WebFrontend.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AIMS.WebFrontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
USER root
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p wwwroot/asset-pictures wwwroot/asset-documents \
    && chown -R app:app wwwroot
USER app
ENTRYPOINT ["dotnet", "AIMS.WebFrontend.dll"]