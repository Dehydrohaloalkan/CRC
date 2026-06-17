# API — Deployment and Usage

## Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Returns `{"status":"ok"}` |
| `POST` | `/crc/bytes` | Raw body → `{"crc":"XXXXXXXX"}` |
| `POST` | `/crc/file` | Multipart upload → `{"crc":"...","filename":"...","size":N}` |
| `GET` | `/swagger` | Interactive API documentation (development) |

### POST /crc/bytes

Send any binary data as the request body:

```bash
curl -s -X POST http://localhost:8080/crc/bytes \
     --data-binary @file.bin
# → {"crc":"22896B0A"}
```

### POST /crc/file

Upload a file via multipart form (field name must be `file`):

```bash
curl -s -X POST http://localhost:8080/crc/file \
     -F "file=@document.pdf"
# → {"crc":"XXXXXXXX","filename":"document.pdf","size":204800}
```

---

## Deployment options

### Option 1 — Docker (recommended)

```bash
# Build image (run from repo root)
docker build -f src/Crc.Api/Dockerfile -t crc-api .

# Run
docker run -d -p 8080:8080 --name crc-api crc-api

# Test
curl http://localhost:8080/health
```

#### docker-compose.yml

```yaml
services:
  crc-api:
    image: crc-api
    build:
      context: .
      dockerfile: src/Crc.Api/Dockerfile
    ports:
      - "8080:8080"
    restart: unless-stopped
```

---

### Option 2 — Direct (Kestrel)

Requires .NET 10 runtime on the server.

```bash
# Copy dist/api/ to server, then:
cd /opt/crc-api
dotnet Crc.Api.dll --urls "http://0.0.0.0:8080"
```

#### Behind nginx (recommended for production)

```nginx
server {
    listen 80;
    server_name crc.example.com;

    location / {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        client_max_body_size 2g;
    }
}
```

---

### Option 3 — systemd service (Linux)

```ini
# /etc/systemd/system/crc-api.service
[Unit]
Description=CRC-32 API
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/crc-api
ExecStart=/usr/bin/dotnet /opt/crc-api/Crc.Api.dll
Environment=ASPNETCORE_URLS=http://0.0.0.0:8080
Restart=on-failure
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload
systemctl enable crc-api
systemctl start crc-api
```

---

## Configuration

`appsettings.json` key settings:

| Key | Default | Description |
|---|---|---|
| `Kestrel.Limits.MaxRequestBodySize` | 2 GB | Max upload size |
| `Logging.LogLevel.Default` | `Information` | Log verbosity |

Override via environment variables:
```bash
ASPNETCORE_URLS=http://0.0.0.0:9000 dotnet Crc.Api.dll
Kestrel__Limits__MaxRequestBodySize=536870912 dotnet Crc.Api.dll
```
