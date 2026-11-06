FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /source
COPY global.json Directory.Build.props NuGet.Config ./
COPY src/ ./src/
RUN dotnet restore src/StockMon.Worker/StockMon.Worker.csproj --locked-mode
RUN dotnet publish src/StockMon.Worker/StockMon.Worker.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.14@sha256:4f1f30cf0bb4ab0c8b2daa84613051683456f8df4a9bf7c812e20d74962992ac
WORKDIR /app
COPY --from=build /out/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "StockMon.Worker.dll"]
CMD ["fake-once"]
