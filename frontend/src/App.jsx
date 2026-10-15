import { useCallback, useEffect, useState } from 'react'
import { api } from './api'
import CrawlPanel from './CrawlPanel'
import { SOURCES } from './sources'

const LEVELS = ['Intern', 'Fresher', 'Junior', 'Middle', 'Senior', 'Lead', 'Manager', 'Unspecified']
const PAGE_SIZE = 50

export default function App() {
  const [filters, setFilters] = useState({ source: '', category: '', level: '', q: '', includeNonIt: false })
  const [page, setPage] = useState(1)
  const [jobs, setJobs] = useState({ total: 0, items: [] })
  const [stats, setStats] = useState(null)
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    const [j, s] = await Promise.all([
      api.jobs({ ...filters, page, pageSize: PAGE_SIZE }),
      api.stats(filters.includeNonIt),
    ])
    setJobs(j)
    setStats(s)
  }, [filters, page])

  useEffect(() => {
    load().catch((e) => setMessage(e.message))
  }, [load])

  const setFilter = (key, value) => {
    setPage(1)
    setFilters((f) => ({ ...f, [key]: f[key] === value && key !== 'q' ? '' : value }))
  }

  const reclassify = async () => {
    const r = await api.reclassify()
    setMessage(`Đã phân loại lại ${r.total} job, ${r.changed} job thay đổi nhãn.`)
    load()
  }

  const pages = Math.max(1, Math.ceil(jobs.total / PAGE_SIZE))

  return (
    <main>
      <header>
        <h1>IT Job Crawler</h1>
        <p>Crawl việc làm IT từ ITViec, TopCV, TopDev, VietnamWorks và phân loại theo mảng / cấp bậc.</p>
      </header>

      <CrawlPanel onProgress={load} />

      <section className="panel">
        <div className="row spread">
          <h2>2. Phân loại</h2>
          <div className="row">
            <label className="check">
              <input type="checkbox" checked={filters.includeNonIt}
                onChange={(e) => setFilter('includeNonIt', e.target.checked)} />
              Hiện cả job không phải IT {stats ? `(${stats.nonIt})` : ''}
            </label>
            <button onClick={reclassify}>Phân loại lại</button>
          </div>
        </div>
        {message && <p className="hint">{message}</p>}
        {stats && (
          <>
            <Facet title="Mảng" items={stats.byCategory} active={filters.category} onPick={(v) => setFilter('category', v)} />
            <Facet title="Cấp bậc" items={stats.byLevel} active={filters.level} onPick={(v) => setFilter('level', v)}
              order={LEVELS} />
            <Facet title="Nguồn" items={stats.bySource} active={filters.source} onPick={(v) => setFilter('source', v)}
              label={(k) => SOURCES.find((s) => s.key === k)?.label ?? k} />
          </>
        )}
      </section>

      <section className="panel">
        <div className="row spread">
          <input className="search" placeholder="Tìm theo tiêu đề, công ty, kỹ năng…" value={filters.q}
            onChange={(e) => setFilter('q', e.target.value)} />
          <span className="hint">{jobs.total} job</span>
        </div>
        <div className="table-wrap">
          <table className="jobs">
            <thead>
              <tr><th>Vị trí</th><th>Công ty</th><th>Địa điểm</th><th>Lương</th><th>Mảng</th><th>Cấp bậc</th><th>Nguồn</th></tr>
            </thead>
            <tbody>
              {jobs.items.map((j) => (
                <tr key={j.id} className={j.isIt ? '' : 'non-it'}>
                  <td>
                    <a href={j.url} target="_blank" rel="noreferrer">{j.title}</a>
                    {j.skills && <div className="skills">{j.skills}</div>}
                  </td>
                  <td>{j.company}</td>
                  <td>{j.location}</td>
                  <td>{j.salary ?? '—'}</td>
                  <td><span className="badge">{j.category}</span></td>
                  <td>{j.level}</td>
                  <td>{SOURCES.find((s) => s.key === j.source)?.label ?? j.source}</td>
                </tr>
              ))}
              {jobs.items.length === 0 && (
                <tr><td colSpan={7} className="hint">Chưa có dữ liệu — bấm “Bắt đầu crawl”.</td></tr>
              )}
            </tbody>
          </table>
        </div>
        <div className="row pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Trước</button>
          <span>Trang {page} / {pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>Sau ›</button>
        </div>
      </section>
    </main>
  )
}

function Facet({ title, items, active, onPick, order, label = (k) => k }) {
  const sorted = order
    ? [...items].sort((a, b) => order.indexOf(a.key) - order.indexOf(b.key))
    : items
  return (
    <div className="facet">
      <span className="facet-title">{title}</span>
      {sorted.map((it) => (
        <button key={it.key} className={`chip ${active === it.key ? 'active' : ''}`} onClick={() => onPick(it.key)}>
          {label(it.key)} <b>{it.count}</b>
        </button>
      ))}
    </div>
  )
}
