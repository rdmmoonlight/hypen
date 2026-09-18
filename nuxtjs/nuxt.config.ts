import tailwindcss from '@tailwindcss/vite'

export default defineNuxtConfig({
  // 1. Modul utama (Tambahkan @nuxthub/core dan modul UI lainnya dari package.json)
  modules: [
    '@nuxthub/core',
    '@nuxt/icon',
    'shadcn-nuxt'
  ],

  // 2. Konfigurasi NuxtHub (Disesuaikan untuk fitur Cloudflare/NuxtHub)
  hub: {
    database: true, // Aktifkan jika menggunakan NuxtHub Database (D1)
    blob: true,     // Aktifkan jika menggunakan NuxtHub Blob (R2)
    kv: true,       // Aktifkan jika menggunakan NuxtHub KV
  },

  // 3. Import CSS utama (Tailwind v4)
  css: ['~/assets/css/main.css'],

  // 4. Fitur kompatibilitas Nuxt 4
  future: {
    compatibilityVersion: 4,
  },

  // 5. Plugin Vite untuk Tailwind CSS v4
  vite: {
    plugins: [
      tailwindcss()
    ]
  },

  // 6. Filter ekstensi komponen agar tidak bentrok dengan file index.ts shadcn/reka-ui
  components: [
    {
      path: '~/components',
      extensions: ['.vue'],
    },
  ],

  // 7. Environment Variables & Runtime Config
  runtimeConfig: {
    youtubeApiKey: process.env.YOUTUBE_API_KEY,
    public: {}
  },

  // 8. Nitro Engine untuk Deployment Cloudflare (Bukan Vercel)
  nitro: {
    preset: 'cloudflare-pages', // Gunakan 'cloudflare-pages' atau 'cloudflare-module' untuk Workers
    experimental: {
      wasm: true // Opsional: Membantu skenario tertentu saat menjalankan engine Prisma di Cloudflare
    }
  },

  // 9. Penanganan Prisma Client agar tidak dibundle secara salah oleh Vite/Nitro
  vite: {
    plugins: [
      tailwindcss()
    ],
    optimizeDeps: {
      include: ['@prisma/client']
    }
  },

  experimental: {
    payloadExtraction: false
  }
})
