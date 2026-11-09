FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# SkiaSharp's native library needs fontconfig, plus at least one font to draw labels with.
# (libgdiplus was only needed by System.Drawing, which this app no longer uses.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
# SkiaSharp allocates image buffers with native malloc from many thread-pool threads; glibc then
# keeps a separate arena per thread (up to 8 x cores) holding freed pages. Two arenas halves the
# resident memory under load (measured: ~200 MB -> ~85 MB anonymous after 60 detections on 2560x1440).
ENV MALLOC_ARENA_MAX=2
WORKDIR /app
# .NET 8+ container images listen on 8080 by default (ASPNETCORE_HTTP_PORTS).
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SynoAI.csproj", "packages.lock.json", "./"]
# Locked mode: the image gets exactly the package graph CI restored and tested,
# instead of re-resolving transitive versions from the bare .csproj.
RUN dotnet restore "SynoAI.csproj" --locked-mode
COPY . .

WORKDIR "/src/."
RUN dotnet build "SynoAI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SynoAI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SynoAI.dll"]