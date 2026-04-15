FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Marriage_Surprise/Marriage_Surprise.csproj Marriage_Surprise/
RUN dotnet restore Marriage_Surprise/Marriage_Surprise.csproj

COPY Marriage_Surprise/ Marriage_Surprise/
WORKDIR /src/Marriage_Surprise
RUN dotnet publish Marriage_Surprise.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data
EXPOSE 10000
ENTRYPOINT ["dotnet", "Marriage_Surprise.dll"]
