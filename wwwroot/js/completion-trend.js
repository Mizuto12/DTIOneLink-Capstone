// Dashboard's Completion Trend widget (see DashboardController.GetCompletionTrend):
// loads the viewer's own weekly completion counts (own work / own division /
// office-wide, decided by the server from session role) and replaces the
// skeleton placeholder already in the page. Shared across the SuperAdmin,
// Admin, and Employee dashboards — each just needs the same container ids.
//
// Rendered as a sparkline (connected line, not bars): with data this sparse
// — usually seven zero weeks and one real one — a bar chart reads as eight
// separate measurements where a line reads as a single trajectory. Hovering
// (or focusing) any point shows its full date and count via a native SVG
// <title>, since the day-number labels below the line are deliberately
// terse ("5", not "Oct 5" — see weekLabels()) to avoid colliding with their
// neighbors at rail width.
(function () {
  const chart = document.getElementById('completionTrendChart');
  const scopeEl = document.getElementById('completionTrendScope');
  const subEl = document.getElementById('completionTrendSub');
  if (!chart) return;

  const SVG_NS = 'http://www.w3.org/2000/svg';
  const MONTH_ABBR = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const MONTH_FULL = ['January', 'February', 'March', 'April', 'May', 'June', 'July',
    'August', 'September', 'October', 'November', 'December'];

  function monthDay(isoDateTime) {
    const d = new Date(isoDateTime);
    return `${MONTH_ABBR[d.getMonth()]} ${d.getDate()}`;
  }

  function fullMonthDay(isoDateTime) {
    const d = new Date(isoDateTime);
    return `${MONTH_FULL[d.getMonth()]} ${d.getDate()}`;
  }

  // Per-bar labels at rail width have roughly 28px to work with — even the
  // shortest month+day combo ("Oct 5") overflows that and collides with its
  // neighbor. A bare day number ("5", "12", "19") is the one thing that
  // always fits, so the month only appears once, spelled out in the
  // subtitle's date range (see render()), and in full on hover per point.
  function weekLabels(weeks) {
    return weeks.map(w => `${new Date(w.weekStart).getDate()}`);
  }

  function svgEl(tag, attrs) {
    const el = document.createElementNS(SVG_NS, tag);
    for (const key in attrs) el.setAttribute(key, attrs[key]);
    return el;
  }

  // Chart geometry, in SVG viewBox units (the viewBox is fixed; the element
  // itself is width:100% in CSS, so this scales to whatever width the rail,
  // the wider Admin column, or the Employee aside actually gives it).
  const VIEW_W = 240;
  const VIEW_H = 90;
  const BASELINE_Y = 80;
  const TOP_Y = 22; // highest a dot can rise to, leaving room for the peak's value label above it
  const MIN_LIFT = 10; // a non-zero point stays visibly off the baseline even when it's tiny next to the peak

  function buildSparkline(weeks) {
    const n = weeks.length;
    const xStep = VIEW_W / n;
    const max = Math.max(...weeks.map(w => w.count), 1);
    const points = weeks.map((w, i) => {
      const x = xStep * i + xStep / 2;
      const isZero = w.count === 0;
      const lift = isZero ? 0 : Math.max(MIN_LIFT, (w.count / max) * (BASELINE_Y - TOP_Y));
      return { x, y: BASELINE_Y - lift, count: w.count, isZero, isPeak: w.count > 0 && w.count === max, date: w.weekStart };
    });

    const svg = svgEl('svg', {
      class: 'ct-spark', viewBox: `0 0 ${VIEW_W} ${VIEW_H}`, width: '100%', height: VIEW_H,
      role: 'img', 'aria-label': 'Weekly completion trend, hover a point for its date',
    });

    svg.appendChild(svgEl('line', {
      class: 'ct-spark__baseline', x1: 0, y1: BASELINE_Y, x2: VIEW_W, y2: BASELINE_Y,
    }));

    const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x},${p.y}`).join(' ');
    const areaPath = `${linePath} L ${points[points.length - 1].x},${BASELINE_Y} L ${points[0].x},${BASELINE_Y} Z`;
    svg.appendChild(svgEl('path', { class: 'ct-spark__area', d: areaPath }));
    svg.appendChild(svgEl('path', { class: 'ct-spark__line', d: linePath }));

    points.forEach(p => {
      const g = svgEl('g', { class: `ct-spark__point${p.isPeak ? ' ct-spark__point--peak' : ''}`, tabindex: '0' });

      const title = document.createElementNS(SVG_NS, 'title');
      title.textContent = `${fullMonthDay(p.date)} · ${p.count} completed`;
      g.appendChild(title);

      // Generous invisible hit area: the visible dot is only ~3-5px, far
      // smaller than a comfortable hover/tap target.
      g.appendChild(svgEl('circle', { class: 'ct-spark__hit', cx: p.x, cy: p.y, r: 11 }));

      if (p.isPeak) {
        g.appendChild(svgEl('circle', { class: 'ct-spark__ring', cx: p.x, cy: p.y, r: 8 }));
        const label = svgEl('text', { class: 'ct-spark__peak-label', x: p.x, y: p.y - 10, 'text-anchor': 'middle' });
        label.textContent = String(p.count);
        g.appendChild(label);
      }
      g.appendChild(svgEl('circle', {
        class: `ct-spark__dot${p.isPeak ? ' ct-spark__dot--peak' : ''}`, cx: p.x, cy: p.y, r: p.isPeak ? 4.5 : 3,
      }));

      svg.appendChild(g);
    });

    return svg;
  }

  function render(data) {
    if (scopeEl) {
      scopeEl.textContent = data.scopeLabel || '';
    }

    const weeks = data.weeks || [];
    if (weeks.length === 0 || weeks.every(w => w.count === 0)) {
      chart.innerHTML = '<p class="ct-empty">No completed tasks in the last 8 weeks yet.</p>';
      return;
    }

    // One number up front, so the headline doesn't require reading every
    // point, plus the actual date range (point labels below are bare day
    // numbers, so this is the only place the month shows at all).
    const total = weeks.reduce((sum, w) => sum + w.count, 0);
    if (subEl) {
      const range = weeks.length > 1
        ? `${monthDay(weeks[0].weekStart)}–${monthDay(weeks[weeks.length - 1].weekStart)}`
        : monthDay(weeks[0].weekStart);
      subEl.textContent = `${total} completed · ${range}`;
    }

    chart.innerHTML = '';
    chart.appendChild(buildSparkline(weeks));

    const labelsRow = document.createElement('div');
    labelsRow.className = 'ct-labels';
    const labels = weekLabels(weeks);
    const max = Math.max(...weeks.map(w => w.count), 1);
    weeks.forEach((w, i) => {
      const span = document.createElement('span');
      span.className = `ct-label${w.count > 0 && w.count === max ? ' ct-label--peak' : ''}`;
      span.textContent = labels[i];
      labelsRow.appendChild(span);
    });
    chart.appendChild(labelsRow);
  }

  fetch('/Dashboard/GetCompletionTrend')
    .then(res => {
      if (!res.ok) throw new Error('Failed to load completion trend');
      return res.json();
    })
    .then(render)
    .catch(() => {
      chart.innerHTML = '<p class="ct-empty">Couldn\'t load the completion trend.</p>';
    })
    .finally(() => {
      chart.classList.remove('ct-skeleton');
      chart.removeAttribute('aria-busy');
    });
})();
