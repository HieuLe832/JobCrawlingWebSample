import { useEffect, useState } from 'react'
import { api } from './api'
import { SOURCES } from './sources'

export default function CrawlPanel({ onProgress }) {
  const [selected, setSelected] = useState(SOURCES.map((s) => s.key))
  const [maxPages, setMaxPages] = useState(2)
  const [status, setStatus] = useState(null)
  const [error, setError] = useState('')

  const running = status?.running

  // Poll while a crawl is running; refresh the job list as batches are saved.
  useEffect(() => {
    let timer
    const tick = async () => {
      try {
        const s = await api.crawlStatus()
        setStatus(s)
        if (s.running) {
          onProgress()
          timer = setTimeout(tick, 2000)
        } else if (running) {
          onProgress()
        }
      } catch (e) {
        setError(e.message)
      }
    }
    tick()
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running])

  const toggle = (key) =>
    setSelected((cur) => (cur.includes(key) ? cur.filter((k) => k !== key) : [...cur, key]))

  const start = async () => {
    setError('')
    try {
      setStatus(await api.startCrawl(selected, maxPages))
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <section className="panel">
      <h2>1. Crawl</h2>
      <div className="row">
        {SOURCES.map((s) => (
          <label key={s.key} className="check">
            <input type="checkbox" checked={selected.includes(s.key)} onChange={() => toggle(s.key)} disabled={running} />
            {s.label}
          </label>
        ))}
        <label className="check">
          Số trang
          <input
            type="number" min={1} max={50} value={maxPages} disabled={running}
            onChange={(e) => setMaxPages(Number(e.target.value))}
            style={{ width: 60 }}
          />
        </label>
        {running ? (
          <button onClick={() => api.cancelCrawl().then(setStatus)}>Huỷ</button>
        ) : (
          <button className="primary" onClick={start} disabled={selected.length === 0}>Bắt đầu crawl</button>
        )}
      </div>
      {error && <p className="error">{error}</p>}
      {status && (
        <table className="status">
          <thead>
            <tr><th>Nguồn</th><th>Trạng thái</th><th>Đã lấy</th><th>Mới</th><th>Cập nhật</th><th>Lỗi</th></tr>
          </thead>
          <tbody>
            {status.sources.map((s) => (
              <tr key={s.source}>
                <td>{SOURCES.find((x) => x.key === s.source)?.label ?? s.source}</td>
                <td><span className={`state ${s.state}`}>{s.state}</span></td>
                <td>{s.fetched}</td>
                <td>{s.inserted}</td>
                <td>{s.updated}</td>
                <td className="error">{s.error}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="hint">Mỗi site được kiểm tra robots.txt trước khi tải và giãn cách 1,5 giây giữa các request. TopDev chậm hơn vì phải mở từng trang chi tiết (~20 job/trang).</p>
    </section>
  )
}
