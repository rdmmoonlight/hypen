<!-- pages/index.vue -->
<script setup lang="ts">
import { Button } from '@/components/ui/button'

const handleEnter = () => {
  // Pindah halaman ke /home
  navigateTo('/home')
}
</script>

<template>
  <div class="relative">
    <!-- Floating Action Button di kanan atas -->
    <div class="fixed top-6 right-6 z-50">
      <Button 
        size="lg" 
        class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 shadow-xl transition-all hover:scale-105 gap-2 cursor-pointer"
        @click="handleEnter"
      >
        <span>Masuk ke Dalam Web</span>
        <Icon name="tabler:arrow-right" class="w-5 h-5" />
      </Button>
    </div>

    <!-- Nuxt Welcome Screen Bawaan -->
    <NuxtWelcome />
  </div>
</template>
