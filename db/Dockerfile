# db/Dockerfile
FROM postgis/postgis:16-3.4

# bullseye-pgdg was moved off the live PGDG mirror; use the archive host
RUN sed -i 's|apt.postgresql.org/pub/repos/apt|apt-archive.postgresql.org/pub/repos/apt|' \
        /etc/apt/sources.list.d/pgdg.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends postgis \
 && rm -rf /var/lib/apt/lists/*
