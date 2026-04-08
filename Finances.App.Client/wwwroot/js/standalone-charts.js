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

    renderLineChart: async function (canvasId, labels, datasets) {
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
                    backgroundColor: (dataset.color || this._colors(index)) + '20',
                    fill: true,
                    tension: 0.3,
                    pointRadius: 3,
                    pointHoverRadius: 5
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top' } },
                scales: {
                    y: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderBarChart: async function (canvasId, labels, datasets) {
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
                plugins: { legend: { position: 'top' } },
                scales: {
                    y: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    x: { grid: { display: false } }
                }
            }
        });
    },

    renderDoughnutChart: async function (canvasId, labels, data, colors) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors || labels.map((_, index) => this._colors(index)),
                    borderWidth: 2,
                    borderColor: '#fff'
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'right' }
                }
            }
        });
    },

    renderForecastChart: async function (canvasId, historicalLabels, actualData, trendData, forecastLabels, forecastData) {
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

        this.instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: allLabels,
                datasets: [
                    {
                        label: 'Actual Revenue',
                        data: actualPadded,
                        borderColor: '#0d9488',
                        backgroundColor: '#0d948820',
                        fill: true,
                        tension: 0.3,
                        pointRadius: 3,
                        pointHoverRadius: 5
                    },
                    {
                        label: 'Trend',
                        data: trendPadded,
                        borderColor: '#6366f1',
                        borderWidth: 2,
                        borderDash: [4, 4],
                        fill: false,
                        tension: 0,
                        pointRadius: 0
                    },
                    {
                        label: 'Forecast',
                        data: forecastPadded,
                        borderColor: '#f59e0b',
                        backgroundColor: '#f59e0b15',
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
                plugins: {
                    legend: { position: 'top' },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                if (context.raw === null) return null;
                                return context.dataset.label + ': ' + context.raw.toFixed(2);
                            }
                        }
                    }
                },
                scales: {
                    y: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    x: { grid: { display: false }, ticks: { maxTicksLimit: 15 } }
                }
            }
        });
    },

    renderStackedBarChart: async function (canvasId, labels, datasets) {
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
                plugins: { legend: { position: 'top' } },
                scales: {
                    x: { stacked: true, grid: { display: false } },
                    y: { stacked: true, beginAtZero: true, grid: { color: '#e5e7eb' } }
                }
            }
        });
    },

    renderHorizontalBarChart: async function (canvasId, labels, data, colors) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors || labels.map((_, index) => this._colors(index)),
                    borderRadius: 4,
                    barThickness: 20
                }]
            },
            options: {
                indexAxis: 'y',
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: {
                    x: { beginAtZero: true, grid: { color: '#e5e7eb' } },
                    y: { grid: { display: false } }
                }
            }
        });
    },

    renderMiniBarChart: async function (canvasId, labels, data, highlightIndex) {
        this._destroy(canvasId);
        const ctx = await this._waitForCanvas(canvasId);
        if (!ctx) return;
        const colors = data.map((_, i) => i === highlightIndex ? '#0d9488' : '#0d948840');
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
                plugins: { legend: { display: false }, tooltip: { enabled: true } },
                scales: {
                    x: { grid: { display: false }, ticks: { font: { size: 11 } } },
                    y: { display: false, beginAtZero: true }
                }
            }
        });
    },

    _destroy: function (canvasId) {
        if (this.instances[canvasId]) {
            this.instances[canvasId].destroy();
            delete this.instances[canvasId];
        }
    },

    _colors: function (index) {
        const palette = ['#0d9488', '#3b82f6', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899', '#10b981', '#6366f1'];
        return palette[index % palette.length];
    }
};