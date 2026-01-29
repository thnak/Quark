# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Directory.Build.props", "./"]
COPY ["Quark.slnx", "./"]
COPY ["src/", "src/"]
COPY ["examples/Quark.Examples.PizzaTracker.Shared/", "examples/Quark.Examples.PizzaTracker.Shared/"]
COPY ["examples/Quark.Examples.PizzaTracker.Console/", "examples/Quark.Examples.PizzaTracker.Console/"]

# Restore dependencies
RUN dotnet restore examples/Quark.Examples.PizzaTracker.Console/Quark.Examples.PizzaTracker.Console.csproj

# Build the application
RUN dotnet build examples/Quark.Examples.PizzaTracker.Console/Quark.Examples.PizzaTracker.Console.csproj -c Release -o /app/build

# Publish stage - create AOT-compiled binary
FROM build AS publish
RUN dotnet publish examples/Quark.Examples.PizzaTracker.Console/Quark.Examples.PizzaTracker.Console.csproj \
    -c Release \
    -o /app/publish \
    --self-contained true \
    -r linux-x64

# Runtime stage - use minimal base image for AOT
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["./Quark.Examples.PizzaTracker.Console"]
