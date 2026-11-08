FROM postgres:18-alpine
RUN apk add --no-cache bash curl rclone redis tzdata
COPY backup.sh entrypoint.sh /usr/local/bin/
COPY crontab /etc/crontabs/root
RUN chmod +x /usr/local/bin/backup.sh /usr/local/bin/entrypoint.sh
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["crond", "-f", "-l", "6"]
