FROM node:22-bookworm-slim
# Libraries Chrome Headless Shell needs (the list in the Remotion docs for Debian). Remotion downloads that browser below;
# it draws video frames far faster than the distribution's Chromium.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg ca-certificates fonts-liberation \
      libnss3 libdbus-1-3 libatk1.0-0 libatk-bridge2.0-0 libgbm1 libasound2 libxrandr2 libxkbcommon0 \
      libxfixes3 libxcomposite1 libxdamage1 libpango-1.0-0 libcairo2 libcups2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY shared/video-composition ./shared/video-composition
COPY media-worker/package*.json ./media-worker/
WORKDIR /app/media-worker
RUN npm ci --omit=dev --no-audit --no-fund \
    && node_modules/.bin/remotion browser ensure \
    && chown -R node:node node_modules/.remotion \
    && rm -rf /usr/local/lib/node_modules /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg /opt/yarn-*
COPY media-worker/tsconfig.json ./
COPY media-worker/src ./src
# CHROME_EXECUTABLE is left unset on purpose: Remotion then uses the headless shell installed above.
USER node
CMD ["node_modules/.bin/tsx", "src/worker.ts"]
