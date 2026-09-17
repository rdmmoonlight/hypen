import { prisma } from '../utils/prisma'
export default defineEventHandler(async () => {
  const [rawCount, completedCount] = await Promise.all([
    prisma.rawSongs.count(),
    prisma.song.count()
  ])
  return { rawCount, completedCount }
})
