FROM node:22-bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg chromium ca-certificates fonts-liberation && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY shared/video-composition ./shared/video-composition
COPY media-worker/package*.json ./media-worker/
WORKDIR /app/media-worker
RUN npm ci --omit=dev --no-audit --no-fund \
    && rm -rf /usr/local/lib/node_modules /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg /opt/yarn-*
COPY media-worker/tsconfig.json ./
COPY media-worker/src ./src
ENV CHROME_EXECUTABLE=/usr/bin/chromium
USER node
CMD ["node_modules/.bin/tsx", "src/worker.ts"]
