FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY cinema-showtime-uae/ .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
CMD ["dotnet", "cinema-showtime-uae.dll"]
