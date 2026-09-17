export default defineNuxtConfig({
  modules: ['@nuxt/icon'],
  runtimeConfig: {
    youtubeApiKey: process.env.YOUTUBE_API_KEY
  },
  nitro: { preset: 'vercel' },
  build: { transpile: [] }
})
