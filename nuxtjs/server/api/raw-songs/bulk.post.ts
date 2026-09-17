import { prisma } from '../../utils/prisma'
export default defineEventHandler(async (event) => {
  const body = await readBody(event) as any[]
  if (!Array.isArray(body) || body.length === 0) throw createError({ statusCode: 400, statusMessage: 'Empty body' })
  const data = body.map((i: any) => ({
    youtubeVideoId: i.YoutubeVideoId || i.FileName,
    fileName: i.FileName,
    title: i.Title,
    artist: i.Artist
  }))
  await prisma.rawSongs.createMany({ data, skipDuplicates: true })
  return { inserted: data.length }
})
