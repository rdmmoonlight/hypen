<!-- pages/index.vue -->
<script setup lang="ts">
const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <div>
    <!-- Nuxt Welcome Screen Bawaan -->
    <NuxtWelcome />

    <!-- Teleport ke body untuk melepaskan diri dari CSS NuxtWelcome -->
    <ClientOnly>
      <Teleport to="body">
        <div class="fixed top-6 right-6 z-[999999]">
          <Button 
            size="lg" 
            class="shadow-2xl hover:scale-105 transition-all gap-2 cursor-pointer"
            @click="handleEnter"
          >
            <span>Masuk ke Dalam Web</span>
            <Icon name="tabler:arrow-right" class="w-5 h-5" />
          </Button>
        </div>
      </Teleport>
    </ClientOnly>
  </div>
</template>
