# Build stage: full SDK, discarded once publish is done.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the csproj alone first so `restore` is cached independently of your
# source. Without this split, every code edit re-downloads every package.
COPY NflPicks.Web.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app #--no-restore

# Runtime stage: ASP.NET runtime only, no SDK, no source.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Listen on all interfaces, not just loopback -- this is what makes the app
# reachable from your phone rather than only from the host.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NflPicks.Web.dll"]
