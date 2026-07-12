FROM node:22-alpine AS build
WORKDIR /app

ARG NUXT_API_BASE
ENV NUXT_API_BASE=${NUXT_API_BASE}

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
COPY --from=build /app/.output ./.output

EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
