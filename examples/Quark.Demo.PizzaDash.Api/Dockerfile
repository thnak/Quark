# Dockerfile for Customer API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Quark.slnx .
COPY Directory.Build.props .
COPY src/ src/
COPY examples/Quark.Demo.PizzaDash.Shared/ examples/Quark.Demo.PizzaDash.Shared/
COPY examples/Quark.Demo.PizzaDash.Api/ examples/Quark.Demo.PizzaDash.Api/

# Restore and build
WORKDIR /src/examples/Quark.Demo.PizzaDash.Api
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quark.Demo.PizzaDash.Api.dll"]
