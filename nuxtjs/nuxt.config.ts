// nuxt.config.ts
export default defineNuxtConfig({
  // Modul utama yang digunakan
  modules: ['@nuxt/icon'],

  // Konfigurasi CSS utama (pilih salah satu path yang kamu gunakan di project)
  css: ['~/assets/css/main.css'],

  // Fitur kompatibilitas Nuxt 4 (jika menggunakan struktur folder app/)
  future: {
    compatibilityVersion: 4,
  },

  // Environment Variables & Runtime Config
  runtimeConfig: {
    youtubeApiKey: process.env.YOUTUBE_API_KEY, // Hanya tersedia di server-side (Nitro)
    public: {
      // Masukkan variabel client-side di sini jika ada (misal: process.env.NEXT_PUBLIC_...)
    }
  },

  // Nitro engine untuk deployment Vercel
  nitro: {
    preset: 'vercel'
  },

  // Fitur eksperimental opsional untuk optimasi rendering
  experimental: {
    payloadExtraction: false
  }
})
