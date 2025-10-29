# Dockerfile (ROS2용)
FROM ros:humble-ros-base

# 1) 필수 패키지
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      git curl python3-colcon-common-extensions \
      ros-humble-teleop-twist-keyboard && \
    rm -rf /var/lib/apt/lists/*

# 2) 워크스페이스
ENV ROS_WS=/root/ros2_ws
RUN mkdir -p ${ROS_WS}/src
WORKDIR ${ROS_WS}

# 3) ROS–TCP–Endpoint (ROS2 릴리스 아카이브 사용)
#   - ROS2v0.7.0 태그를 받아서 src/ 아래에 펼친 뒤 빌드
RUN curl -L https://github.com/Unity-Technologies/ROS-TCP-Endpoint/archive/refs/tags/ROS2v0.7.0.tar.gz \
      -o /tmp/ros_tcp_endpoint_ros2.tgz && \
    tar -xzf /tmp/ros_tcp_endpoint_ros2.tgz -C ${ROS_WS}/src && \
    mv ${ROS_WS}/src/ROS-TCP-Endpoint-ROS2v0.7.0/* ${ROS_WS}/src/ && \
    rm -rf ${ROS_WS}/src/ROS-TCP-Endpoint-ROS2v0.7.0 /tmp/ros_tcp_endpoint_ros2.tgz

# 4) 빌드(ROS2 패키지: ros_tcp_endpoint)
RUN bash -lc "source /opt/ros/humble/setup.bash && colcon build --packages-select ros_tcp_endpoint"

# 5) 쉘 환경
RUN echo 'source /opt/ros/humble/setup.bash' >> /root/.bashrc && \
    echo 'source /root/ros2_ws/install/setup.bash' >> /root/.bashrc

EXPOSE 10000
CMD ["bash", "-lc", "source /opt/ros/humble/setup.bash && source /root/ros2_ws/install/setup.bash && ros2 run ros_tcp_endpoint default_server_endpoint"]

