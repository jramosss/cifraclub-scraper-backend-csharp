const http = require('http');

const PORT = process.env.FETCHER_PORT || 5001;

const server = http.createServer(async (req, res) => {
  try {
    const reqUrl = new URL(req.url, `http://127.0.0.1:${PORT}`);
    const targetUrl = reqUrl.searchParams.get('url');

    if (!targetUrl) {
      res.writeHead(400, { 'Content-Type': 'text/plain' });
      res.end('Missing url parameter');
      return;
    }

    const upstream = await fetch(targetUrl, {
      headers: {
        'User-Agent': 'Mozilla/5.0',
        'Accept': '*/*',
        'Accept-Language': '*'
      }
    });

    const body = await upstream.text();
    res.writeHead(upstream.status, {
      'Content-Type': upstream.headers.get('content-type') || 'text/html; charset=utf-8'
    });
    res.end(body);
  } catch (err) {
    res.writeHead(500, { 'Content-Type': 'text/plain' });
    res.end(err.message || 'Fetch error');
  }
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`[Fetcher] Running on http://127.0.0.1:${PORT}`);
});
