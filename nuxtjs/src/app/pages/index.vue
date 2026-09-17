<!-- pages/index.vue -->
<script setup lang="ts">
// Impor atau panggil komponen yang kamu butuhkan
import { Button } from '@/components/ui/button'
</script>

<template>
  <main class="min-h-screen bg-background text-foreground p-8">
    <div class="max-w-4xl mx-auto space-y-6">
      <h1 class="text-3xl font-bold">Selamat Datang di Web</h1>
      <p class="text-muted-foreground">
        Halaman utama aplikasi berhasil dimuat tanpa NuxtWelcome.
      </p>

      <Button size="lg" class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold">
        Jelajahi Fitur
      </Button>
    </div>
  </main>
</template>
