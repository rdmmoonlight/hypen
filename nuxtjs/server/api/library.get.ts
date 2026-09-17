import { prisma } from '../utils/prisma'
export default defineEventHandler(async (event) => {
  const { search } = getQuery(event) as { search?: string }
  return await prisma.song.findMany({
    where: search? {
      OR: [
        { title: { contains: search, mode: 'insensitive' } },
        { artist: { contains: search, mode: 'insensitive' } }
      ]
    } : {},
    orderBy: { createdAt: 'desc' },
    take: 500
  })
})
