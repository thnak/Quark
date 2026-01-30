# Dockerfile for Kitchen Display
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Quark.slnx .
COPY Directory.Build.props .
COPY src/ src/
COPY examples/Quark.Demo.PizzaDash.Shared/ examples/Quark.Demo.PizzaDash.Shared/
COPY examples/Quark.Demo.PizzaDash.KitchenDisplay/ examples/Quark.Demo.PizzaDash.KitchenDisplay/

# Restore and build
WORKDIR /src/examples/Quark.Demo.PizzaDash.KitchenDisplay
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Quark.Demo.PizzaDash.KitchenDisplay.dll"]
