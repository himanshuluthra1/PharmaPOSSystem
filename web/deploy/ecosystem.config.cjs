module.exports = {
  apps: [
    {
      name: "pharmapos-dashboard",
      cwd: "/var/www/mypos",
      script: "node_modules/next/dist/bin/next",
      args: "start -p 3010",
      instances: 1,
      exec_mode: "fork",
      env: {
        NODE_ENV: "production",
        PORT: "3010",
      },
    },
  ],
};
