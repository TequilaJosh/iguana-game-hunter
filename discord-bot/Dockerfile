FROM node:20-alpine

WORKDIR /app

# ffmpeg: decodes the TTS audio for the voice-channel reader (@discordjs/voice).
RUN apk add --no-cache ffmpeg

# Install production dependencies first (better layer caching).
COPY package*.json ./
RUN npm install --omit=dev

# App source.
COPY . .

ENV NODE_ENV=production
# Hosts usually inject PORT; default matches .env.example.
EXPOSE 8080

CMD ["node", "src/index.js"]
