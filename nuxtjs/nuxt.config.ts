// nuxt.config.ts
export default defineNuxtConfig({
  // Modul utama yang digunakan (Tambahkan @nuxtjs/tailwindcss)
  modules: [
    '@nuxt/icon',
    '@nuxtjs/tailwindcss'
  ],

  // Path CSS utama disesuaikan ke struktur Nuxt 4 (app/assets/css/main.css)
  css: ['~/app/assets/css/main.css'],

  // Mengaktifkan fitur kompatibilitas Nuxt 4 secara penuh
  future: {
    compatibilityVersion: 4,
  },

  // Konfigurasi registrasi komponen untuk mencegah peringatan ganda (UiButton)
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
