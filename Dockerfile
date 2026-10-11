FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Papey.csproj .
RUN dotnet restore Papey.csproj
COPY . .
# Publish the PROJECT (not the slnx) and let it re-run restore. Do NOT use
# --no-restore here: restoring from the csproj alone yields a graph without the
# microsoft.aspnetcore.app.internal.assets package, and publish then silently skips
# wwwroot/_framework/blazor.web.js -> 404 at runtime -> no Blazor Server
# interactivity (color/size selection, cart, admin actions, ...).
RUN dotnet publish Papey.csproj -c Release -o /app/publish \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js \
    && echo "OK: blazor.web.js found"

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Papey.dll"]
