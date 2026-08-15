FROM node:20-alpine

WORKDIR /app

# Install production dependencies first (better layer caching).
COPY package*.json ./
RUN npm install --omit=dev

# App source.
COPY . .

ENV NODE_ENV=production
# Hosts usually inject PORT; default matches .env.example.
EXPOSE 8080

CMD ["node", "src/index.js"]
