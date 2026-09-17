import { prisma } from '../utils/prisma'
export default defineEventHandler(async () => {
  return await prisma.rawSongs.findMany({ orderBy: { createdAt: 'desc' }, take: 100 })
})
