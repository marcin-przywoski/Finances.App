window.financeCharts = {
    instances: {},

    // Wait for the canvas to exist in the DOM and have non-zero dimensions.
    // Returns the canvas element, or null if it doesn't appear within ~500ms.
    _waitForCanvas: function (canvasId) {
        return new Promise(resolve => {
            let attempts = 0;
            const check = () => {
                const el = document.getElementById(canvasId);
                if (el && el.offsetParent !== null) {
                    resolve(el);
                } else if (++attempts < 25) {
                    requestAnimationFrame(check);
                } else {
                    // Last-resort fallback: return the element even if not laid out yet
                    resolve(el || null);
                }
            };
            requestAnimationFrame(check);
        });
    },

    // All colors come from the CSS custom properties in standalone-app.css,
    // so charts follow the active theme; re-rendering after a theme change
    // picks up the new values.
    _token: function (name, fallback) {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return value || fallback;
    },

    _palette: function () {
        return [
            this._token('--primary', '#0d9488'),
            this._token('--info', '#3b82f6'),
            this._token('--warning', '#f59e0b'),
            this._token('--danger', '#ef4444'),
            this._token('--purple', '#8b5cf6'),
            this._token('--pink', '#ec4899'),
            this._token('--success', '#10b981'),
            this._token('--indigo', '#6366f1')
        ];
    },

    _colors: function (index) {
        const palette = this._palette();
        return palette[index % palette.length];
    },

    _grid: function () {
        return this._token('--border-color', '#e5e7eb');
    },

    // Adds a translucent alpha channel to a token color (hex or rgb/rgba).
    _alpha: function (color, alpha) {
        if (color.startsWith('#')) {
            const hex = Math.round(alpha * 255).toString(16).padStart(2, '0');
            return color.length === 7 ? color + hex : color;
        }
        const match = color.match(/rgba?\(([^)]+)\)/);
        if (match) {
            const parts = match[1].split(',').slice(0, 3).join(',');
            return `rgba(${parts}, ${alpha})`;
        }
        return color;
    },

    // money = { locale, currency } (either may be null). Returns a formatter
    // for tooltip labels and axis ticks; falls back to plain numbers.
    _moneyFormatter: function (money) {
        try {
            if (money && money.currency) {
                return new Intl.NumberFormat(money.locale || undefined, {
                    style: 'currency',
                    currency: money.currency
                });
            }
            return new Intl.NumberFormat((money && money.locale) || undefined, {
                maximumFractionDigits: 2
            });
        } catch {
            return { format: value => String(value) };
        }
    },

    _moneyPlugins: function (money, legendPosition) {
        const fmt = this._moneyFormatter(money);
        return {
            legend: legendPosition === 'none' ? { display: false } : { position: legendPosition || 'top' },
            tooltip: {
                callbacks: {
                    label: context => {
                        if (context.raw === null || context.raw === undefined) return null;
                        const label = context.dataset.label ? context.dataset.label + ': ' : '';
                        return label + fmt.format(context.raw);
                    }
                }
            }
        };
    },

    _moneyTicks: function (money) {
        const fmt = this._moneyFormatter(money);
        return { callback: value => fmt.format(value) };
    },

    renderLineChart: async function (canvasId, labels, datasets, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: datasets.map((dataset, index) => ({
                    label: dataset.label,
                    data: dataset.data,
                    borderColor: dataset.color || this._colors(index),
                    backgroundColor: this._alpha(dataset.color || this._colors(index), 0.13),
                    fill: true,
                    tension: 0.3,
                    pointRadius: 3,
                    pointHoverRadius: 5
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: this._moneyPlugins(money, 'top'),
                scales: {
                    y: { beginAtZero: true, grid: { color: this._grid() }, ticks: this._moneyTicks(money) },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderBarChart: async function (canvasId, labels, datasets, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: datasets.map((dataset, index) => ({
                    label: dataset.label,
                    data: dataset.data,
                    backgroundColor: dataset.color || this._colors(index),
                    borderRadius: 4
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: this._moneyPlugins(money, 'top'),
                scales: {
                    y: { beginAtZero: true, grid: { color: this._grid() }, ticks: this._moneyTicks(money) },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderDoughnutChart: async function (canvasId, labels, data, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        const fmt = this._moneyFormatter(money);
        this.instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: labels.map((_, index) => this._colors(index)),
                    borderWidth: 2,
                    borderColor: this._token('--card-bg', '#fff')
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'right' },
                    tooltip: {
                        callbacks: {
                            label: context => `${context.label}: ${fmt.format(context.raw)}`
                        }
                    }
                }
            }
        });
    },

    renderForecastChart: async function (canvasId, historicalLabels, actualData, trendData, forecastLabels, forecastData, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;

        const allLabels = [...historicalLabels, ...forecastLabels];
        const actualPadded = [...actualData, ...forecastLabels.map(() => null)];
        const trendPadded = [...trendData, ...forecastLabels.map(() => null)];
        const forecastPadded = [...historicalLabels.map(() => null)];

        if (trendData.length > 0) {
            forecastPadded[forecastPadded.length - 1] = trendData[trendData.length - 1];
        }

        forecastPadded.push(...forecastData);

        const primary = this._token('--primary', '#0d9488');
        const indigo = this._token('--indigo', '#6366f1');
        const warning = this._token('--warning', '#f59e0b');

        this.instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: allLabels,
                datasets: [
                    {
                        label: 'Actual Revenue',
                        data: actualPadded,
                        borderColor: primary,
                        backgroundColor: this._alpha(primary, 0.13),
                        fill: true,
                        tension: 0.3,
                        pointRadius: 3,
                        pointHoverRadius: 5
                    },
                    {
                        label: 'Trend',
                        data: trendPadded,
                        borderColor: indigo,
                        borderWidth: 2,
                        borderDash: [4, 4],
                        fill: false,
                        tension: 0,
                        pointRadius: 0
                    },
                    {
                        label: 'Forecast',
                        data: forecastPadded,
                        borderColor: warning,
                        backgroundColor: this._alpha(warning, 0.08),
                        borderWidth: 2,
                        borderDash: [6, 3],
                        fill: true,
                        tension: 0,
                        pointRadius: 2,
                        pointStyle: 'triangle'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: this._moneyPlugins(money, 'top'),
                scales: {
                    y: { beginAtZero: true, grid: { color: this._grid() }, ticks: this._moneyTicks(money) },
                    x: { grid: { display: false }, ticks: { maxTicksLimit: 15 } }
                }
            }
        });
    },

    renderStackedBarChart: async function (canvasId, labels, datasets, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: datasets.map((dataset, index) => ({
                    label: dataset.label,
                    data: dataset.data,
                    backgroundColor: dataset.color || this._colors(index),
                    borderRadius: 4
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: this._moneyPlugins(money, 'top'),
                scales: {
                    x: { stacked: true, grid: { display: false } },
                    y: { stacked: true, beginAtZero: true, grid: { color: this._grid() }, ticks: this._moneyTicks(money) }
                }
            }
        });
    },

    renderHorizontalBarChart: async function (canvasId, labels, data, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        const fmt = this._moneyFormatter(money);
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: labels.map((_, index) => this._colors(index)),
                    borderRadius: 4,
                    barThickness: 20
                }]
            },
            options: {
                indexAxis: 'y',
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: context => fmt.format(context.raw)
                        }
                    }
                },
                scales: {
                    x: { beginAtZero: true, grid: { color: this._grid() }, ticks: this._moneyTicks(money) },
                    y: { grid: { display: false } }
                }
            }
        });
    },

    renderMiniBarChart: async function (canvasId, labels, data, highlightIndex, money) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        const primary = this._token('--primary', '#0d9488');
        const colors = data.map((_, i) => i === highlightIndex ? primary : this._alpha(primary, 0.25));
        const fmt = this._moneyFormatter(money);
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors,
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: context => fmt.format(context.raw)
                        }
                    }
                },
                scales: {
                    x: { grid: { display: false }, ticks: { font: { size: 11 } } },
                    y: { display: false, beginAtZero: true }
                }
            }
        });
    },

    // Public: called from page DisposeAsync and from empty-data branches so a
    // chart for a canvas that left the DOM never lingers.
    destroy: function (canvasId) {
        this._destroy(canvasId);
    },

    destroyMany: function (canvasIds) {
        for (const id of canvasIds) {
            this._destroy(id);
        }
    },

    _destroy: function (canvasId) {
        if (this.instances[canvasId]) {
            this.instances[canvasId].destroy();
            delete this.instances[canvasId];
        }
    }
};
