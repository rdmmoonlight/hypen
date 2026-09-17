<!-- pages/index.vue -->
<script setup lang="ts">
import { Button } from '@/components/ui/button'

const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <div class="relative w-screen h-screen overflow-hidden">
    <!-- Floating Action Button (Ditaruh SEBELUM NuxtWelcome dengan fixed + inset/z-index khusus Nuxt 4) -->
    <div class="fixed top-6 right-6 z-[9999999] pointer-events-auto">
      <Button 
        size="lg" 
        class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 shadow-2xl transition-all hover:scale-105 gap-2 cursor-pointer relative"
        @click="handleEnter"
      >
        <span>Masuk ke Dalam Web</span>
        <ClientOnly>
          <Icon name="tabler:arrow-right" class="w-5 h-5" />
        </ClientOnly>
      </Button>
    </div>

    <!-- NuxtWelcome Nuxt 4 dibatasi agar z-index nya tidak menguasai viewport root -->
    <div class="absolute inset-0 z-0 pointer-events-none [&_*]:pointer-events-auto">
      <NuxtWelcome />
    </div>
  </div>
</template>
