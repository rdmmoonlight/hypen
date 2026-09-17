import tailwindcss from '@tailwindcss/vite'

export default defineNuxtConfig({
  // Modul utama yang digunakan
  modules: [
    '@nuxt/icon',
  ],

  // Cukup '~/assets/css/main.css'
  // Di Nuxt 4 (compatibilityVersion: 4), alias '~' sudah otomatis mengarah ke folder app/
  css: ['~/assets/css/main.css'],

  // Fitur kompatibilitas Nuxt 4
  future: {
    compatibilityVersion: 4,
  },

  vite: {
    plugins: [
      tailwindcss()
    ]
  },
  
  // Filter ekstensi komponen agar tidak ada peringatan UiButton ganda dari index.ts
  components: [
    {
      path: '~/components',
      extensions: ['.vue'],
    },
  ],

  // Environment Variables & Runtime Config
  runtimeConfig: {
    youtubeApiKey: process.env.YOUTUBE_API_KEY,
    public: {}
  },

  // Nitro engine untuk deployment Vercel
  nitro: {
    preset: 'vercel'
  },

  experimental: {
    payloadExtraction: false
  }
})
