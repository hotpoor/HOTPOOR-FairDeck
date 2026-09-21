FROM mirror.ccs.tencentyun.com/library/python:3.12-slim-bookworm
WORKDIR /app
COPY server/requirements.txt server/requirements.txt
RUN pip install --no-cache-dir -r server/requirements.txt
COPY core core
COPY server server
USER 10001:10001
CMD ["python", "-m", "server.app"]
