import { chromium } from 'playwright'

const BASE = process.argv[2] ?? 'https://staging.ovcupirim.az'
const browser = await chromium.launch()

try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  await page.goto(BASE, { waitUntil: 'networkidle' })

  const kataloq = page.getByRole('button', { name: 'Kataloq' })
  console.log('aria-expanded (əvvəl):', await kataloq.getAttribute('aria-expanded'))

  await kataloq.click()
  await page.getByRole('navigation', { name: 'Kateqoriyalar', exact: true }).waitFor({ timeout: 10000 })
  console.log('aria-expanded (sonra):', await kataloq.getAttribute('aria-expanded'))

  const panel = page.locator('#catalogue-menu')
  const box = await panel.boundingBox()
  console.log('panel:', JSON.stringify({
    x: Math.round(box.x), y: Math.round(box.y),
    w: Math.round(box.width), h: Math.round(box.height),
    bg: await panel.evaluate((el) => getComputedStyle(el).backgroundColor),
  }))

  const first = page.getByRole('navigation', { name: 'Kateqoriyalar', exact: true }).getByRole('link').first()
  console.log('ilk kateqoriya:', (await first.innerText()).trim())

  await page.screenshot({ path: 'catalogue-open.png', clip: { x: 0, y: 0, width: 1440, height: 640 } })

  // Pointing at the second category must swap the right-hand column.
  const second = page.getByRole('navigation', { name: 'Kateqoriyalar', exact: true }).getByRole('link').nth(1)
  const secondName = (await second.innerText()).trim()
  await second.hover()
  await page.waitForTimeout(400)
  const swapped = await page
    .getByRole('navigation', { name: `${secondName} alt kateqoriyaları` })
    .isVisible()
  console.log(`hover "${secondName}" -> sağ sütun dəyişdi:`, swapped)

  // The search field beside the button must stay usable while the panel is open.
  const searchBox = await page.getByLabel('Avadanlıq və ya marka axtarışı').boundingBox()
  const onTop = await page.evaluate(
    ({ x, y }) => {
      const el = document.elementFromPoint(x, y)
      return el?.tagName + '.' + (el?.getAttribute('aria-label') ?? el?.className?.toString().slice(0, 30))
    },
    { x: searchBox.x + searchBox.width / 2, y: searchBox.y + searchBox.height / 2 },
  )
  console.log('axtarış xanasının üstündəki element:', onTop)

  await page.keyboard.press('Escape')
  await page.waitForTimeout(300)
  console.log('Escape ilə bağlandı:', (await kataloq.getAttribute('aria-expanded')) === 'false')

  console.log('üfüqi sürüşmə:', await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth))
} finally {
  await browser.close()
}
