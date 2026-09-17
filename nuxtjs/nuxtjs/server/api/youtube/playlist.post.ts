export default defineEventHandler(async (event) => {
  const { playlistId } = await readBody(event)
  return Array.from({ length: 5 }).map((_, i) => ({
    videoId: `yt_${(playlistId||'test').slice(0,8)}_${i}`,
    title: `YouTube Track ${i+1}`,
    channelTitle: `Channel ${i+1}`
  }))
})
