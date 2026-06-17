FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Crc.Core/Crc.Core.csproj", "src/Crc.Core/"]
COPY ["src/Crc.Api/Crc.Api.csproj",   "src/Crc.Api/"]
COPY ["Directory.Build.props", "./"]
RUN dotnet restore "src/Crc.Api/Crc.Api.csproj"

COPY . .
RUN dotnet publish "src/Crc.Api/Crc.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Crc.Api.dll"]
